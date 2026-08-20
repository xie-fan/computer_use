using System.Collections.Concurrent;
using ComputerUse.Mcp.Native;

namespace ComputerUse.Mcp.Native;

internal sealed class NativeStaDispatcher : IDisposable
{
    private readonly BlockingCollection<WorkItem> _queue = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly nint[] _waitHandles;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private bool _disposed;

    public NativeStaDispatcher()
    {
        _waitHandles = [_wake.SafeWaitHandle.DangerousGetHandle()];
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
        var item = new WorkItem(cancellationToken, () =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
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
            _wake.Set();
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }

        return tcs.Task.WaitAsync(cancellationToken);
    }

    private void Pump()
    {
        _ = NativeMethods.OleInitialize(0);
        _ready.Set();
        try
        {
            while (true)
            {
                PumpMessages();
                while (_queue.TryTake(out var item))
                {
                    item.Run();
                    PumpMessages();
                }

                if (_queue.IsAddingCompleted)
                    break;

                _ = NativeMethods.MsgWaitForMultipleObjectsEx(
                    1,
                    _waitHandles,
                    NativeMethods.INFINITE,
                    NativeMethods.QS_ALLINPUT,
                    NativeMethods.MWMO_INPUTAVAILABLE);
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
        _wake.Set();
        if (!_thread.Join(TimeSpan.FromSeconds(2)))
        {
            // Background STA thread is abandoned on process exit.
        }
        _queue.Dispose();
        _wake.Dispose();
        _ready.Dispose();
    }

    private sealed class WorkItem(CancellationToken token, Action run)
    {
        public void Run()
        {
            try
            {
                token.ThrowIfCancellationRequested();
                run();
            }
            catch (OperationCanceledException)
            {
                // WaitAsync already cancelled the waiter; never unwind the STA pump.
            }
        }
    }
}
