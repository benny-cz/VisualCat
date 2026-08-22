using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using VisualCat.App.Presentation;
using VisualCat.App.Views;

namespace VisualCat.App.Tests;

/// <summary>
/// An imported session with a live workspace over it, torn down together.
/// </summary>
/// <remarks>
/// The Android live-test remediation needs the same shape as the earlier review passes, but
/// at more than one window size — several of its findings are about what the layout does when
/// the viewport is short (landscape) rather than what it does at 1280 × 800.
/// </remarks>
internal sealed class LiveTestWorkspaceFixture : IAsyncDisposable
{
    private readonly string _root;
    private readonly WorkspaceViewModel _workspace;

    private LiveTestWorkspaceFixture(
        string root,
        WorkspaceViewModel workspace,
        SessionTabViewModel tab,
        SessionWorkspaceView view,
        Window window)
    {
        _root = root;
        _workspace = workspace;
        Tab = tab;
        View = view;
        Window = window;
    }

    public SessionTabViewModel Tab { get; }

    public Window Window { get; }

    public SessionWorkspaceView View { get; }

    public WorkspaceViewModel Workspace => _workspace;

    public ListBox Entries => View.GetLogicalDescendants()
        .OfType<ListBox>()
        .Single(static list => AutomationProperties.GetName(list) == "Filtered log entries");

    public static async Task<LiveTestWorkspaceFixture> CreateAsync(
        string log,
        double width = 1280,
        double height = 800)
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        WorkspaceViewModel.ConfigureTemporarySessionRoot(root);
        var sourcePath = Path.Combine(root, "session.txt");
        await File.WriteAllTextAsync(sourcePath, log, TestContext.Current.CancellationToken);
        var workspace = new WorkspaceViewModel();
        var tab = await workspace.ImportFileAsync(sourcePath, TestContext.Current.CancellationToken);
        var view = new SessionWorkspaceView(tab);
        var window = new Window { Content = view, Width = width, Height = height };
        window.Show();
        return new LiveTestWorkspaceFixture(root, workspace, tab, view, window);
    }

    public async ValueTask DisposeAsync()
    {
        Window.Close();
        await _workspace.CloseAsync(Tab);
        await _workspace.DisposeAsync();
        WorkspaceViewModel.ConfigureTemporarySessionRoot(null);
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
