using System.Collections.Specialized;
using System.Globalization;
using System.Text;
using Avalonia.Headless.XUnit;
using VisualCat.App.Presentation;

namespace VisualCat.App.Tests;

/// <summary>
/// Closing a session has to end the readers that hold its load lock, not queue behind them.
/// </summary>
/// <remarks>
/// <para>
/// Paging every row of a view holds the load lock across the whole walk. Its only
/// cancellation used to live in the view that started it, and nothing cancelled that when
/// the tab closed, so <see cref="SessionTabViewModel.DisposeAsync"/> — which waits for that
/// same lock before it can release the snapshot — queued behind a walk that was still
/// appending rows into the session it was tearing down. Because
/// <c>WorkspaceViewModel.CloseAsync</c> raises <c>TabRemoved</c> only after disposal
/// returns, the tab stayed on screen and unclosable for the duration; closing the
/// application took the same path with no window left to explain why the process would not
/// exit.
/// </para>
/// <para>
/// The tests below pin the mechanism rather than a duration. Paging is fast enough at any
/// size a test can afford to build that a stopwatch would measure scheduling noise, so what
/// is asserted is the observable consequence: a walk interrupted by disposal stops where it
/// was instead of running to the end.
/// </para>
/// </remarks>
public sealed class SessionTabDisposalTests
{
    private const int SessionLines = 8_000;

    [AvaloniaFact]
    public async Task DisposalStopsAnInFlightLoadAllInsteadOfWaitingForIt()
    {
        await WithImportedSessionAsync(async (workspace, tab) =>
        {
            _ = workspace;
            var total = tab.Statistics?.TotalMatching ?? 0;
            Assert.Equal(SessionLines, total);
            Assert.True(tab.CanLoadMore);

            // The view owns the load-all cancellation, exactly as SessionWorkspaceView does.
            // Disposal must not depend on it: this token is never cancelled here.
            using var viewCancellation = new CancellationTokenSource();

            // Close the tab from inside the first batch that lands, which is the shape of a
            // reader pressing the tab's close button while the rows are still arriving.
            // CollectionChanged runs on the UI thread inside the paging loop's own dispatcher
            // callback, and DisposeAsync cancels the session lifetime before its first await,
            // so the walk sees the cancellation on its very next iteration.
            var disposal = (Task?)null;
            void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs args)
            {
                if (disposal is null && tab.Entries.Count > SessionTabViewModel.EntryPageSize)
                {
                    tab.Entries.CollectionChanged -= OnEntriesChanged;
                    disposal = tab.DisposeAsync().AsTask();
                }
            }

            tab.Entries.CollectionChanged += OnEntriesChanged;

            await tab.LoadAllEntriesAsync(viewCancellation.Token);

            Assert.NotNull(disposal);
            await disposal;

            Assert.True(tab.IsDisposed);
            Assert.False(viewCancellation.IsCancellationRequested);

            // The walk stopped where the close found it. Before the session lifetime existed
            // it ran to the last row and only then let disposal proceed.
            Assert.True(
                tab.Entries.Count < total,
                $"The load-all walk ran to completion ({tab.Entries.Count:N0} of {total:N0} rows) despite the session closing underneath it.");
        });
    }

    /// <summary>
    /// A closing session does not report its own teardown to the reader as a failed action.
    /// </summary>
    /// <remarks>
    /// <c>SessionWorkspaceView.ToggleLoadAllEntriesAsync</c> treats its own cancellation as an
    /// ordinary user action and anything else as a fault it writes into the status line. A
    /// cancellation that came from disposal is neither, so it must not escape as an exception.
    /// </remarks>
    [AvaloniaFact]
    public async Task DisposalDuringLoadAllDoesNotSurfaceAsAFailure()
    {
        await WithImportedSessionAsync(async (workspace, tab) =>
        {
            _ = workspace;
            using var viewCancellation = new CancellationTokenSource();
            var disposal = (Task?)null;
            void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs args)
            {
                if (disposal is null && tab.Entries.Count > SessionTabViewModel.EntryPageSize)
                {
                    tab.Entries.CollectionChanged -= OnEntriesChanged;
                    disposal = tab.DisposeAsync().AsTask();
                }
            }

            tab.Entries.CollectionChanged += OnEntriesChanged;

            // The assertion is that this does not throw.
            var exception = await Record.ExceptionAsync(() => tab.LoadAllEntriesAsync(viewCancellation.Token));
            Assert.Null(exception);

            Assert.NotNull(disposal);
            await disposal;
        });
    }

    /// <summary>
    /// A reader's own Stop still reads as cancellation, and still leaves the loaded rows.
    /// </summary>
    [AvaloniaFact]
    public async Task ReaderCancellationOfLoadAllIsStillObservedAsCancellation()
    {
        await WithImportedSessionAsync(async (workspace, tab) =>
        {
            _ = workspace;
            using var viewCancellation = new CancellationTokenSource();
            void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs args)
            {
                if (tab.Entries.Count > SessionTabViewModel.EntryPageSize)
                {
                    tab.Entries.CollectionChanged -= OnEntriesChanged;
                    viewCancellation.Cancel();
                }
            }

            tab.Entries.CollectionChanged += OnEntriesChanged;

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => tab.LoadAllEntriesAsync(viewCancellation.Token));

            Assert.False(tab.IsDisposed);
            Assert.True(tab.Entries.Count > SessionTabViewModel.EntryPageSize);
            Assert.True(tab.Entries.Count < SessionLines);
        });
    }

    /// <summary>
    /// Requests that a view raised before the tab closed, and the dispatcher delivered after,
    /// decline quietly rather than reaching a semaphore and a token that are already gone.
    /// </summary>
    [AvaloniaFact]
    public async Task RequestsArrivingAfterDisposalDeclineQuietly()
    {
        await WithImportedSessionAsync(async (workspace, tab) =>
        {
            _ = workspace;
            var entry = tab.Entries.First();
            await tab.DisposeAsync();
            Assert.True(tab.IsDisposed);

            var loaded = tab.Entries.Count;
            Assert.Null(await Record.ExceptionAsync(() => tab.LoadAllEntriesAsync()));
            Assert.Null(await Record.ExceptionAsync(() => tab.LoadNextEntryPageAsync()));
            Assert.Null(await Record.ExceptionAsync(() => tab.RefreshAsync()));
            Assert.Null(await Record.ExceptionAsync(() => tab.LoadSnapshotAsync(final: true)));
            Assert.Null(await Record.ExceptionAsync(() => tab.LoadRawContextAsync(entry)));
            Assert.Equal(loaded, tab.Entries.Count);

            // Copying raw bytes has a return value the caller acts on, so it says the session
            // is gone rather than quietly answering with nothing.
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => tab.ReadRawEntriesAsync([entry]));
        });
    }

    /// <summary>Disposal is idempotent, and a second one does not wait on the first's lock.</summary>
    [AvaloniaFact]
    public async Task DisposalIsIdempotent()
    {
        await WithImportedSessionAsync(async (workspace, tab) =>
        {
            _ = workspace;
            await tab.DisposeAsync();
            await tab.DisposeAsync();
            Assert.True(tab.IsDisposed);
        });
    }

    private static async Task WithImportedSessionAsync(
        Func<WorkspaceViewModel, SessionTabViewModel, Task> body)
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        WorkspaceViewModel.ConfigureTemporarySessionRoot(root);
        try
        {
            var sourcePath = Path.Combine(root, "disposal-session.txt");
            await File.WriteAllTextAsync(sourcePath, BuildLog(SessionLines), TestContext.Current.CancellationToken);

            await using var workspace = new WorkspaceViewModel();
            var tab = await workspace.ImportFileAsync(sourcePath, TestContext.Current.CancellationToken);
            await body(workspace, tab);
        }
        finally
        {
            // The session's segments are memory mapped, so the directory only becomes
            // removable once the workspace above has been disposed. A leftover temporary
            // directory is not worth failing a test over.
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static string BuildLog(int lines)
    {
        var builder = new StringBuilder(lines * 96);
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var index = 0; index < lines; index++)
        {
            var level = index % 5 == 0 ? 'E' : 'I';
            var tag = level == 'E' ? "ErrorTag" : "Worker";
            var message = level == 'E' ? $"request {index} failure code 500" : $"request {index} completed";
            builder.Append(start.AddMilliseconds(index).ToString("MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
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
