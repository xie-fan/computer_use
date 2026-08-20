using System.Collections.Concurrent;
using ComputerUse.Mcp.Native;

namespace ComputerUse.Mcp.Native;

internal sealed class NativeStaDispatcher : IDisposable
{
    private readonly BlockingCollection<WorkItem> _queue = new();
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private bool _disposed;

    public NativeStaDispatcher()
    {
        _thread = new Thread(Pump)
        {
            IsBackground = true,
            Name = "ComputerUse.STA"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
    }

    public Task InvokeAsync(Action action, CancellationToken cancellationToken) =>
        InvokeAsync(0, () =>
        {
            action();
            return 0;
        }, cancellationToken);

    private Task<T> InvokeAsync<T>(T _, Func<T> func, CancellationToken cancellationToken) =>
        InvokeAsync(func, cancellationToken);

    public Task<T> InvokeAsync<T>(Func<T> func, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new WorkItem(() =>
        {
            try
            {
                tcs.TrySetResult(func());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        try
        {
            _queue.Add(item, cancellationToken);
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }

        return tcs.Task.WaitAsync(cancellationToken);
    }

    public async Task<T> InvokeAsync<T>(Func<Task<T>> func, CancellationToken cancellationToken)
    {
        return await InvokeAsync(() => func().GetAwaiter().GetResult(), cancellationToken).ConfigureAwait(false);
    }

    private void Pump()
    {
        _ = NativeMethods.OleInitialize(0);
        _ready.Set();
        try
        {
            while (!_queue.IsAddingCompleted)
            {
                if (_queue.TryTake(out var item, 15))
                    item.Run();
                PumpMessages();
            }
        }
        finally
        {
            NativeMethods.OleUninitialize();
        }
    }

    private static void PumpMessages()
    {
        while (NativeMethods.PeekMessage(out var msg, 0, 0, 0, NativeMethods.PM_REMOVE))
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _queue.CompleteAdding();
        if (!_thread.Join(TimeSpan.FromSeconds(2)))
        {
            // Background STA thread is abandoned on process exit.
        }
        _queue.Dispose();
        _ready.Dispose();
    }

    private sealed class WorkItem(Action run)
    {
        public void Run() => run();
    }
}
