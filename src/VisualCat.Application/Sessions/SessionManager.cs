using System.Collections.Concurrent;
using VisualCat.Core.Store;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Time;

namespace VisualCat.Application.Sessions;

public sealed class SessionHandle : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private int _disposed;

    internal SessionHandle(SessionSnapshot snapshot)
    {
        Snapshot = snapshot;
        Filter = FilterSpec.All;
        Viewport = snapshot.TimedRange is { } range ? new Viewport(range, 1000) : null;
    }

    public SessionSnapshot Snapshot { get; }
    public FilterSpec Filter { get; set; }
    public Viewport? Viewport { get; set; }
    public CancellationToken LifetimeToken => _lifetime.Token;

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        _lifetime.Cancel();
        Snapshot.Dispose();
        _lifetime.Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class SessionManager : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, SessionHandle> _sessions = new();
    private readonly SemaphoreSlim _resourceGovernor;

    public SessionManager(int maximumConcurrentImports = 2)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumConcurrentImports);
        _resourceGovernor = new SemaphoreSlim(maximumConcurrentImports, maximumConcurrentImports);
    }

    public IReadOnlyCollection<SessionHandle> Sessions => _sessions.Values.ToArray();

    public SessionHandle Add(SessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var handle = new SessionHandle(snapshot);
        if (!_sessions.TryAdd(snapshot.SessionId, handle))
        {
            snapshot.Dispose();
            throw new InvalidOperationException($"Session {snapshot.SessionId} is already open.");
        }

        return handle;
    }

    public bool TryGet(Guid sessionId, out SessionHandle? handle) => _sessions.TryGetValue(sessionId, out handle);

    public async Task<TResult> GovernImportAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        await _resourceGovernor.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await action(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _resourceGovernor.Release();
        }
    }

    public async Task<bool> CloseAsync(Guid sessionId)
    {
        if (!_sessions.TryRemove(sessionId, out var handle))
        {
            return false;
        }

        await handle.DisposeAsync().ConfigureAwait(false);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var sessionId in _sessions.Keys)
        {
            await CloseAsync(sessionId).ConfigureAwait(false);
        }

        _resourceGovernor.Dispose();
    }
}
