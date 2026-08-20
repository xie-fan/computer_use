using ComputerUse.Mcp.Coordination;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Identity;
using ComputerUse.Mcp.Native;
using ComputerUse.Mcp.Tests.Fakes;

namespace ComputerUse.Mcp.Tests;

public sealed class OperationIdCacheTests
{
    [Fact]
    public void Complete_KeepsOriginalStartedAt()
    {
        var cache = new OperationIdCache(Limits.V1);
        Assert.Null(cache.TryBegin("op-1"));
        Thread.Sleep(25);
        cache.Complete("op-1", 1, true);
        var cached = cache.TryBegin("op-1");
        Assert.NotNull(cached);
        Assert.True(cached.CompletedAt > cached.StartedAt);
    }
}

public sealed class NativeStaDispatcherTests
{
    [Fact]
    public async Task InvokeAsync_RunsOnStaThread()
    {
        using var sta = new NativeStaDispatcher();
        var apt = await sta.InvokeAsync(() => Thread.CurrentThread.GetApartmentState(), CancellationToken.None);
        Assert.Equal(ApartmentState.STA, apt);
    }

    [Fact]
    public async Task InvokeAsync_HonorsCancellationBeforeRun()
    {
        using var sta = new NativeStaDispatcher();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sta.InvokeAsync(() => 1, cts.Token));
    }
}

public sealed class TargetTokenRevokeTests
{
    [Fact]
    public void RevokedSet_EvictsOldestWhenOverCap()
    {
        var world = new FakeWorld();
        world.Processes[10] = new FakeProcess { Pid = 10, CreateTimeUtc = 1000 };
        world.Windows[1] = new FakeWindow { Hwnd = 1, Pid = 10, ClassName = "Notepad" };
        var tokens = new TargetTokenService();
        var first = tokens.Issue(1, 10, 1000, "Notepad");
        tokens.Revoke(first);
        Assert.Throws<ComputerUseException>(() => tokens.RequireValid(first, world, world));

        for (var i = 0; i < TargetTokenService.MaxRevoked; i++)
            tokens.Revoke("revoked-" + i);

        var again = tokens.RequireValid(first, world, world);
        Assert.Equal(10u, again.Pid);
    }
}
