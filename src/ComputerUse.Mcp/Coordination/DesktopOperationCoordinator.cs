using ComputerUse.Mcp.Domain;

namespace ComputerUse.Mcp.Coordination;

internal sealed class DesktopOperationCoordinator
{
    private readonly Limits _limits;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _inflight;

    public DesktopOperationCoordinator(Limits limits)
    {
        _limits = limits;
    }

    public int InFlight => Volatile.Read(ref _inflight);

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _inflight) > _limits.MaxQueuedOperations)
        {
            Interlocked.Decrement(ref _inflight);
            throw new ComputerUseException(ErrorCodes.Busy, "The desktop operation queue is full.");
        }

        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(_limits.RequestDeadlineMs);
            try
            {
                await _gate.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw new ComputerUseException(ErrorCodes.Cancelled, "The operation was cancelled.");
            }
            catch (OperationCanceledException)
            {
                throw new ComputerUseException(ErrorCodes.Timeout, "Timed out waiting for the desktop coordinator.");
            }

            try
            {
                return await work(deadline.Token).ConfigureAwait(false);
            }
            catch (ComputerUseException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw new ComputerUseException(ErrorCodes.Cancelled, "The operation was cancelled.");
            }
            catch (OperationCanceledException)
            {
                throw new ComputerUseException(ErrorCodes.Timeout, "The desktop operation exceeded the request deadline.");
            }
            finally
            {
                _gate.Release();
            }
        }
        finally
        {
            Interlocked.Decrement(ref _inflight);
        }
    }
}

internal sealed class OperationIdCache
{
    private readonly Limits _limits;
    private readonly object _gate = new();
    private readonly Dictionary<string, CachedOperation> _items = new(StringComparer.Ordinal);

    public OperationIdCache(Limits limits)
    {
        _limits = limits;
    }

    public CachedOperation? TryBegin(string operationId)
    {
        lock (_gate)
        {
            PurgeUnlocked(DateTimeOffset.UtcNow);
            if (_items.TryGetValue(operationId, out var existing))
            {
                if (!existing.OutcomeKnown)
                    return existing;
                if (DateTimeOffset.UtcNow - existing.CompletedAt <= TimeSpan.FromMilliseconds(_limits.OperationIdTtlMs))
                    return existing;
            }

            _items[operationId] = new CachedOperation
            {
                OutcomeKnown = false,
                StartedAt = DateTimeOffset.UtcNow
            };
            return null;
        }
    }

    public void Complete(string operationId, object result, bool outcomeKnown, bool isError = false, string? code = null, string? message = null)
    {
        lock (_gate)
        {
            _items[operationId] = new CachedOperation
            {
                OutcomeKnown = outcomeKnown,
                IsError = isError,
                Code = code,
                Message = message,
                Result = result,
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow
            };
        }
    }

    private void PurgeUnlocked(DateTimeOffset now)
    {
        var ttl = TimeSpan.FromMilliseconds(_limits.OperationIdTtlMs);
        var stale = _items.Where(kv => kv.Value.OutcomeKnown && now - kv.Value.CompletedAt > ttl)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var key in stale)
            _items.Remove(key);
    }
}

internal sealed class CachedOperation
{
    public bool OutcomeKnown { get; init; }
    public bool IsError { get; init; }
    public string? Code { get; init; }
    public string? Message { get; init; }
    public object? Result { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
}
