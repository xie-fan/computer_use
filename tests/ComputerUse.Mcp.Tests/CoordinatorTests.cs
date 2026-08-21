using System.Diagnostics;
using ComputerUse.Mcp.Coordination;
using ComputerUse.Mcp.Domain;

namespace ComputerUse.Mcp.Tests;

public sealed class CoordinatorTests
{
    [Fact]
    public async Task QueueFull_ReturnsBusy()
    {
        var limits = Limits.V1 with { MaxQueuedOperations = 2, RequestDeadlineMs = 10_000 };
        var coordinator = new DesktopOperationCoordinator(limits);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = coordinator.RunAsync(async ct =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(ct);
            return 1;
        }, CancellationToken.None);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = coordinator.RunAsync(async ct =>
        {
            secondStarted.TrySetResult();
            await Task.Delay(50, ct);
            return 2;
        }, CancellationToken.None);

        var waited = 0;
        while (coordinator.InFlight < 2 && waited < 40)
        {
            await Task.Delay(25);
            waited++;
        }
        Assert.Equal(2, coordinator.InFlight);

        var busy = await Assert.ThrowsAsync<ComputerUseException>(() =>
            coordinator.RunAsync(_ => Task.FromResult(3), CancellationToken.None));
        Assert.Equal(ErrorCodes.Busy, busy.Code);

        release.TrySetResult();
        Assert.Equal(1, await first);
        Assert.Equal(2, await second);
        Assert.True(secondStarted.Task.IsCompleted);
    }

    [Fact]
    public async Task SerializesOperations()
    {
        var coordinator = new DesktopOperationCoordinator(Limits.V1 with { MaxQueuedOperations = 4 });
        var order = new List<int>();
        var firstHold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var a = coordinator.RunAsync(async _ =>
        {
            order.Add(1);
            await firstHold.Task;
            order.Add(2);
            return 0;
        }, CancellationToken.None);

        await Task.Delay(30);
        var b = coordinator.RunAsync(_ =>
        {
            order.Add(3);
            return Task.FromResult(0);
        }, CancellationToken.None);

        await Task.Delay(30);
        Assert.Equal(new[] { 1 }, order);
        firstHold.TrySetResult();
        await Task.WhenAll(a, b);
        Assert.Equal(new[] { 1, 2, 3 }, order);
    }

    [Fact]
    public async Task FirstOpBoundByInnerTimeout_UnblocksQueuedOpBeforeRequestDeadline()
    {
        var limits = Limits.V1 with { RequestDeadlineMs = 8_000, ZnccSearchTimeoutMs = 80 };
        var coordinator = new DesktopOperationCoordinator(limits);
        var sw = Stopwatch.StartNew();

        var first = coordinator.RunAsync(async ct =>
        {
            using var search = CancellationTokenSource.CreateLinkedTokenSource(ct);
            search.CancelAfter(limits.ZnccSearchTimeoutMs);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), search.Token);
            }
            catch (OperationCanceledException)
            {
                return "timed-out";
            }

            return "finished";
        }, CancellationToken.None);

        var second = coordinator.RunAsync(_ => Task.FromResult("second"), CancellationToken.None);
        Assert.Equal("timed-out", await first);
        Assert.Equal("second", await second);
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), sw.Elapsed.ToString());
    }
}
