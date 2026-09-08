using System.ComponentModel;
using System.Runtime.CompilerServices;
using VisualCat.Application.UseCases;

namespace VisualCat.App.Presentation;

public enum FileOperationKind : byte
{
    Open,
    Export,
    Save,
    Share,
    Archive,
    Diagnostics,
}

public enum FileOperationPhase : byte
{
    Preparing,
    Running,
    Cancelling,
    Publishing,
}

public enum FilePublicationStatus : byte
{
    NotPublished,
    LocalCommitted,
    ProviderDeliveryStarted,
    ProviderDeliveryCompleted,
}

public enum FileOperationOutcome : byte
{
    Succeeded,
    Cancelled,
    Failed,
}

/// <summary>
/// What the shell knows about the one file operation it is running.
/// </summary>
/// <remarks>
/// <c>ShowsCard</c> is false once preparation has handed its work to the tab that imports the
/// file: the tab reports its own progress, and two indicators for one action is one too many,
/// so the card goes while the slot stays claimed.
/// </remarks>
public sealed record FileOperationSnapshot(
    Guid OperationId,
    FileOperationKind Kind,
    string Title,
    FileOperationPhase Phase,
    FileWorkProgress? Progress,
    bool CanCancel,
    FilePublicationStatus Publication,
    bool ShowsCard = true);

public sealed record FileOperationResult(
    FileOperationOutcome Outcome,
    FilePublicationStatus Publication,
    Exception? Error = null)
{
    public static FileOperationResult Succeeded(FilePublicationStatus publication) =>
        new(FileOperationOutcome.Succeeded, publication);

    public static FileOperationResult Cancelled(FilePublicationStatus publication) =>
        new(FileOperationOutcome.Cancelled, publication);

    public static FileOperationResult Failed(Exception error, FilePublicationStatus publication) =>
        new(FileOperationOutcome.Failed, publication, error);
}

/// <summary>
/// Owns the one finite shell file operation, its cancellation source, publication boundary,
/// and cleanup wait. It is intentionally not a persistent queue.
/// </summary>
public sealed class FileOperationOwner : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private FileOperationHandle? _active;
    private FileOperationSnapshot? _current;
    private Task _running = Task.CompletedTask;
    private int _disposed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public FileOperationSnapshot? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
        private set
        {
            lock (_gate)
            {
                _current = value;
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
        }
    }

    public bool IsBusy => Current is not null;

    public bool TryBegin(FileOperationKind kind, string title, out FileOperationHandle? operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        lock (_gate)
        {
            if (_disposed != 0 || _active is not null)
            {
                operation = null;
                return false;
            }

            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            operation = new FileOperationHandle(this, Guid.NewGuid(), kind, title, cancellation);
            _active = operation;
            _current = operation.CreateSnapshot();
        }

        RaiseStateChanged();
        return true;
    }

    public void Track(FileOperationHandle operation, Task task)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(task);
        lock (_gate)
        {
            if (ReferenceEquals(_active, operation))
            {
                _running = task;
            }
        }
    }

    public void Cancel()
    {
        FileOperationHandle? operation;
        lock (_gate)
        {
            operation = _active;
        }

        operation?.RequestCancellation();
    }

    internal void Publish(FileOperationHandle operation)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_active, operation))
            {
                return;
            }

            _current = operation.CreateSnapshot();
        }

        RaiseStateChanged();
    }

    internal void End(FileOperationHandle operation)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_active, operation))
            {
                return;
            }

            _active = null;
            _current = null;
            _running = Task.CompletedTask;
        }

        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBusy)));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lifetime.Cancel();
        Cancel();
        Task running;
        lock (_gate)
        {
            running = _running;
        }

        try
        {
            await running.ConfigureAwait(false);
        }
        catch
        {
            // The command reports its own typed terminal result. Disposal only drains it.
        }

        lock (_gate)
        {
            _active?.DisposeCancellation();
            _active = null;
            _current = null;
        }

        _lifetime.Dispose();
    }
}

public sealed class FileOperationHandle : IAsyncDisposable
{
    private readonly FileOperationOwner _owner;
    private readonly CancellationTokenSource _cancellation;
    private readonly object _gate = new();
    private FileOperationPhase _phase = FileOperationPhase.Preparing;
    private FileWorkProgress? _progress;
    private FilePublicationStatus _publication;
    private bool _handedOff;
    private long _lastProgressTick;
    private int _ended;

    internal FileOperationHandle(
        FileOperationOwner owner,
        Guid operationId,
        FileOperationKind kind,
        string title,
        CancellationTokenSource cancellation)
    {
        _owner = owner;
        OperationId = operationId;
        Kind = kind;
        Title = title;
        _cancellation = cancellation;
    }

    public Guid OperationId { get; }
    public FileOperationKind Kind { get; }
    public string Title { get; }
    public CancellationToken Token => _cancellation.Token;
    public FilePublicationStatus Publication
    {
        get
        {
            lock (_gate)
            {
                return _publication;
            }
        }
    }

    public IProgress<FileWorkProgress> Progress => new ProgressSink(this);

    public void Report(FileWorkProgress progress)
    {
        lock (_gate)
        {
            if (_ended != 0 || _phase is FileOperationPhase.Cancelling or FileOperationPhase.Publishing)
            {
                return;
            }

            var now = Environment.TickCount64;
            var stageChanged = _progress?.Stage != progress.Stage || _handedOff;
            _phase = FileOperationPhase.Running;
            _progress = progress;

            // Work of this operation's own again — a second file in the same selection.
            _handedOff = false;
            if (!stageChanged && now - _lastProgressTick < 200)
            {
                return;
            }

            _lastProgressTick = now;
        }

        _owner.Publish(this);
    }

    /// <summary>
    /// Stops drawing the card because another owner is now doing and reporting the work.
    /// </summary>
    /// <remarks>
    /// The slot stays claimed, so a second file command is still refused and still names
    /// what is holding it. Only the duplicate progress display goes.
    /// </remarks>
    public void HandOff()
    {
        lock (_gate)
        {
            if (_ended != 0 || _handedOff)
            {
                return;
            }

            _handedOff = true;
            _progress = null;
        }

        _owner.Publish(this);
    }

    public void SetPublication(FilePublicationStatus publication, bool finalizing = false)
    {
        lock (_gate)
        {
            if (_ended != 0)
            {
                return;
            }

            _publication = publication;
            // A cancellation requested just before the irreversible boundary remains a
            // cancellation request. If the producer nevertheless commits, publication is
            // still updated above and its typed success wins; only the interim card keeps
            // saying Cancelling instead of reversing itself to Finishing.
            if (finalizing && _phase != FileOperationPhase.Cancelling)
            {
                _phase = FileOperationPhase.Publishing;
                _progress = new FileWorkProgress(FileWorkStage.Publishing);
            }
        }

        _owner.Publish(this);
    }

    /// <summary>Begins the short, irreversible local publication boundary.</summary>
    public void BeginPublishing()
    {
        lock (_gate)
        {
            if (_ended != 0 || _phase is FileOperationPhase.Cancelling or FileOperationPhase.Publishing)
            {
                return;
            }

            _phase = FileOperationPhase.Publishing;
            _progress = new FileWorkProgress(FileWorkStage.Publishing);
        }

        _owner.Publish(this);
    }

    public void RequestCancellation()
    {
        lock (_gate)
        {
            if (_ended != 0 || _phase is FileOperationPhase.Cancelling or FileOperationPhase.Publishing)
            {
                return;
            }

            _phase = FileOperationPhase.Cancelling;
        }

        try
        {
            _cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _owner.Publish(this);
    }

    internal FileOperationSnapshot CreateSnapshot()
    {
        lock (_gate)
        {
            return new FileOperationSnapshot(
                OperationId,
                Kind,
                Title,
                _phase,
                _progress,
                _phase is not FileOperationPhase.Cancelling and not FileOperationPhase.Publishing,
                _publication,
                !_handedOff);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _ended, 1) == 0)
        {
            _owner.End(this);
            _cancellation.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    internal void DisposeCancellation() => _cancellation.Dispose();

    private sealed class ProgressSink(FileOperationHandle owner) : IProgress<FileWorkProgress>
    {
        public void Report(FileWorkProgress value) => owner.Report(value);
    }
}
