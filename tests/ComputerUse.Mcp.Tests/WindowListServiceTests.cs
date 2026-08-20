using System.Text.Json;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Identity;
using ComputerUse.Mcp.Services;
using ComputerUse.Mcp.Tests.Fakes;

namespace ComputerUse.Mcp.Tests;

public sealed class HostProcessResolverTests
{
    private static readonly object EnvGate = new();

    [Fact]
    public void DescendantOfHost_IsHostProcess()
    {
        lock (EnvGate)
        {
            var world = new FakeWorld();
            world.Processes[10] = new FakeProcess { Pid = 10, ParentPid = 1, CreateTimeUtc = 1000, Name = "Cursor" };
            world.Processes[11] = new FakeProcess { Pid = 11, ParentPid = 10, CreateTimeUtc = 1100, Name = "helper" };
            world.Processes[99] = new FakeProcess { Pid = 99, ParentPid = 2, CreateTimeUtc = 2000, Name = "notepad" };

            using var env = new HostPidScope(10);
            var host = new HostProcessResolver(world);

            Assert.True(host.IsHostProcess(10, 1000));
            Assert.True(host.IsHostProcess(11, 1100));
            Assert.False(host.IsHostProcess(99, 2000));
        }
    }

    [Fact]
    public void PidReuse_WithNewCreateTime_IsNotHostProcess()
    {
        lock (EnvGate)
        {
            var world = new FakeWorld();
            world.Processes[10] = new FakeProcess { Pid = 10, ParentPid = 1, CreateTimeUtc = 1000 };
            world.Processes[11] = new FakeProcess { Pid = 11, ParentPid = 10, CreateTimeUtc = 1100 };

            using var env = new HostPidScope(10);
            var host = new HostProcessResolver(world);
            Assert.True(host.IsHostProcess(11, 1100));
            Assert.False(host.IsHostProcess(11, 9999));
        }
    }

    [Fact]
    public void HostRelaunch_RebuildsTree()
    {
        lock (EnvGate)
        {
            var world = new FakeWorld();
            world.Processes[10] = new FakeProcess { Pid = 10, ParentPid = 1, CreateTimeUtc = 1000 };
            world.Processes[11] = new FakeProcess { Pid = 11, ParentPid = 10, CreateTimeUtc = 1100 };

            using var env = new HostPidScope(10);
            var host = new HostProcessResolver(world);
            Assert.True(host.IsHostProcess(11, 1100));

            world.Processes[10].CreateTimeUtc = 5000;
            world.Processes[11] = new FakeProcess { Pid = 11, ParentPid = 2, CreateTimeUtc = 1100 };
            world.Processes[12] = new FakeProcess { Pid = 12, ParentPid = 10, CreateTimeUtc = 5100 };
            host.RefreshHostTree();

            Assert.False(host.IsHostProcess(11, 1100));
            Assert.True(host.IsHostProcess(12, 5100));
            Assert.True(host.IsHostProcess(10, 5000));
        }
    }
}

public sealed class WindowListServiceTests
{
    private static readonly object HostEnv = new();

    [Fact]
    public void SamePid_QueriesProcessInfoOnce()
    {
        var world = new FakeWorld();
        world.Processes[10] = new FakeProcess { Pid = 10, CreateTimeUtc = 1000, Name = "app" };
        world.Windows[1] = new FakeWindow { Hwnd = 1, Pid = 10, Title = "A" };
        world.Windows[2] = new FakeWindow { Hwnd = 2, Pid = 10, Title = "B" };
        var host = new StubHost();

        var list = new WindowListService(
            world,
            new FakeMonitors(),
            world,
            new FakeDesktops(),
            host,
            new TargetTokenService(),
            Limits.V1);

        var json = JsonSerializer.SerializeToElement(list.List());
        Assert.Equal(2, json.GetProperty("windows").GetArrayLength());
        Assert.Equal(1, world.InfoCallCount);
        Assert.Equal(1, host.RefreshCount);
    }

    [Fact]
    public void HostChildWindow_IsMarkedHostWindow()
    {
        lock (HostEnv)
        {
            var world = new FakeWorld();
            world.Processes[10] = new FakeProcess { Pid = 10, ParentPid = 1, CreateTimeUtc = 1000, Name = "Cursor" };
            world.Processes[11] = new FakeProcess { Pid = 11, ParentPid = 10, CreateTimeUtc = 1100, Name = "helper" };
            world.Processes[99] = new FakeProcess { Pid = 99, ParentPid = 2, CreateTimeUtc = 2000, Name = "notepad" };
            world.Windows[1] = new FakeWindow { Hwnd = 1, Pid = 11, Title = "Cursor" };
            world.Windows[2] = new FakeWindow { Hwnd = 2, Pid = 99, Title = "Notepad" };

            using var env = new HostPidScope(10);
            var host = new HostProcessResolver(world);
            var list = new WindowListService(
                world,
                new FakeMonitors(),
                world,
                new FakeDesktops(),
                host,
                new TargetTokenService(),
                Limits.V1);
            var json = JsonSerializer.SerializeToElement(list.List());
            var windows = json.GetProperty("windows");
            Assert.True(FindWindow(windows, "Cursor").GetProperty("isHostWindow").GetBoolean());
            Assert.False(FindWindow(windows, "Notepad").GetProperty("isHostWindow").GetBoolean());
        }
    }

    private static JsonElement FindWindow(JsonElement windows, string title)
    {
        foreach (var w in windows.EnumerateArray())
        {
            if (w.GetProperty("title").GetString() == title)
                return w;
        }

        Assert.Fail($"Window '{title}' was not listed.");
        return default;
    }
}

file sealed class HostPidScope : IDisposable
{
    public HostPidScope(uint pid)
    {
        Environment.SetEnvironmentVariable("COMPUTER_USE_HOST_PID", pid.ToString());
    }

    public void Dispose() => Environment.SetEnvironmentVariable("COMPUTER_USE_HOST_PID", null);
}
