using System.Globalization;
using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using VisualCat.App.Presentation;
using VisualCat.App.Views;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Queries;
using VisualCat.Domain.Time;

namespace VisualCat.App.Tests;

public sealed class SessionWorkspaceHeadlessTests
{
    [AvaloniaFact]
    public async Task ActiveCaptureKeepsStopActionVisibleDuringSnapshotRefresh()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await using var tab = new SessionTabViewModel("Live", root)
        {
            IsLiveCaptureActive = true,
            // A progressive snapshot used to overwrite Capturing with Importing and
            // accidentally hide the only graceful-stop action.
            Status = "Importing · 1 committed · snapshot 1",
        };
        var view = new SessionWorkspaceView(tab);
        var window = new Window { Content = view, Width = 900, Height = 600 };
        window.Show();
        try
        {
            var stop = view.GetLogicalDescendants()
                .OfType<Button>()
                .Single(button => Equals(button.Content, "Stop capture"));
            Assert.True(stop.IsVisible);
            Assert.True(stop.IsEnabled);
        }
        finally
        {
            window.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task ImportQueryFilterPagePersistAndComposeWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        WorkspaceViewModel.ConfigureTemporarySessionRoot(root);
        try
        {
            var sourcePath = Path.Combine(root, "headless-logcat.txt");
            await File.WriteAllTextAsync(sourcePath, BuildLog(650), TestContext.Current.CancellationToken);

            await using var workspace = new WorkspaceViewModel();
            var added = 0;
            var removed = 0;
            workspace.TabAdded += (_, _) => added++;
            workspace.TabRemoved += (_, _) => removed++;

            var tab = await workspace.ImportFileAsync(sourcePath, TestContext.Current.CancellationToken);

            Assert.Equal(1, added);
            Assert.Same(tab, workspace.Selected);
            Assert.NotNull(tab.Snapshot);
            Assert.NotNull(tab.HeatMap);
            Assert.NotNull(tab.Overview);
            Assert.NotNull(tab.Statistics);
            Assert.Equal(650, tab.Statistics.TotalMatching);
            Assert.Equal(SessionTabViewModel.EntryPageSize, tab.Entries.Count);
            Assert.True(tab.CanLoadMore);

            await tab.LoadNextEntryPageAsync(TestContext.Current.CancellationToken);
            Assert.Equal(650, tab.Entries.Count);
            Assert.False(tab.CanLoadMore);

            var errorTag = FacetKey.OfText("ErrorTag");
            await tab.ToggleFacetAsync(FacetDimension.Tag, errorTag, exclude: false);
            Assert.Equal(FacetState.Included, tab.StateOf(FacetDimension.Tag, errorTag));
            Assert.Equal(130, tab.Entries.Count);
            Assert.All(tab.Entries, static entry => Assert.Equal("ErrorTag", entry.Tag));

            tab.SearchText = "failure";
            await tab.ApplySearchAsync(regex: false, caseSensitive: false);
            Assert.Equal(130, tab.SearchResult?.Matches);
            Assert.All(tab.Entries, static entry => Assert.Contains("failure", entry.Message));

            await tab.SaveCurrentViewAsync("Failures", TestContext.Current.CancellationToken);
            Assert.Contains("Failures", tab.SavedViews);
            await tab.ClearFiltersAsync();
            Assert.Equal(SessionTabViewModel.EntryPageSize, tab.Entries.Count);
            await tab.ApplySavedViewAsync("Failures", TestContext.Current.CancellationToken);
            Assert.Equal(FacetState.Included, tab.StateOf(FacetDimension.Tag, errorTag));
            Assert.Equal("failure", tab.SearchText);

            await tab.SetEntryOrderAsync(EntryOrder.SourceSequence);
            Assert.Equal(EntryOrder.SourceSequence, tab.EntryOrder);
            Assert.Equal(tab.Entries.OrderBy(static entry => entry.SourceSequence), tab.Entries);

            var selected = Assert.Single(tab.Entries.Take(1));
            await tab.LoadRawContextAsync(selected, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Contains("failure", tab.RawContextText);
            var raw = await tab.ReadRawEntriesAsync([selected], TestContext.Current.CancellationToken);
            Assert.Contains("failure", raw);

            var sessionRange = Assert.IsType<TimeRange>(tab.Snapshot.TimedRange);
            var midpoint = new InstantUs(sessionRange.StartInclusive.Value + sessionRange.DurationUs / 2);
            var firstHalf = new TimeRange(sessionRange.StartInclusive, midpoint);
            var secondHalf = new TimeRange(midpoint, sessionRange.EndExclusive);
            await Task.WhenAll(tab.SetViewportAsync(firstHalf), tab.SetViewportAsync(secondHalf));
            Assert.Equal(secondHalf, tab.Viewport);
            Assert.Equal(secondHalf, tab.HeatMap?.Viewport.Range);

            var view = new SessionWorkspaceView(tab);
            var window = new Window { Content = view, Width = 1280, Height = 800 };
            window.Show();
            try
            {
                var accessibleNames = view.GetLogicalDescendants()
                    .OfType<Control>()
                    .Select(AutomationProperties.GetName)
                    .Where(static name => !string.IsNullOrWhiteSpace(name))
                    .ToArray();
                Assert.Contains("Session filters", accessibleNames);
                Assert.Contains("Filtered log entries", accessibleNames);
                Assert.Contains("Facets", accessibleNames);

                var search = view.GetLogicalDescendants()
                    .OfType<TextBox>()
                    .Single(textBox => textBox.PlaceholderText?.Contains("Search", StringComparison.Ordinal) == true);
                Assert.True(view.TryHandleShortcut(new KeyEventArgs
                {
                    Key = Key.F,
                    KeyModifiers = KeyModifiers.Control,
                }));
                Assert.True(search.IsFocused);

                var entries = view.GetLogicalDescendants()
                    .OfType<ListBox>()
                    .Single(list => AutomationProperties.GetName(list) == "Filtered log entries");
                entries.SelectedIndex = 0;
                Assert.True(view.TryHandleShortcut(new KeyEventArgs { Key = Key.J }));
                Assert.Equal(1, entries.SelectedIndex);
            }
            finally
            {
                window.Close();
            }

            await workspace.CloseAsync(tab);
            Assert.Empty(workspace.Tabs);
            Assert.Equal(1, removed);
        }
        finally
        {
            WorkspaceViewModel.ConfigureTemporarySessionRoot(null);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string BuildLog(int lines)
    {
        var builder = new StringBuilder(lines * 96);
        for (var index = 0; index < lines; index++)
        {
            var level = index % 5 == 0 ? 'E' : 'I';
            var tag = level == 'E' ? "ErrorTag" : "Worker";
            var message = level == 'E' ? $"request {index} failure code 500" : $"request {index} completed";
            builder.Append("01-01 00:00:00.")
                .Append((index * 1_000).ToString("000000", CultureInfo.InvariantCulture))
                .Append("   100   101 ")
                .Append(level)
                .Append(' ')
                .Append(tag.PadRight(15))
                .Append(": ")
                .AppendLine(message);
        }

        return builder.ToString();
    }
}
