using ComputerUse.Mcp.Coordination;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Identity;
using ComputerUse.Mcp.Memory;
using ComputerUse.Mcp.Services;
using ComputerUse.Mcp.Tests.Fakes;
using ComputerUse.Mcp.Tests.Support;
using System.Text.Json;

namespace ComputerUse.Mcp.Tests;

public sealed class ObserveServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cu-obs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Success_HasNoPngPayload()
    {
        var (svc, _, token) = Create(host: false);
        var result = await svc.ObserveAsync(token, CancellationToken.None);
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("data:image", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"Png\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.Visualized);
        Assert.False(string.IsNullOrWhiteSpace(result.FrameId));
    }

    [Fact]
    public async Task HostWindow_EmptyControls()
    {
        var (svc, _, token) = Create(host: true);
        var result = await svc.ObserveAsync(token, CancellationToken.None);
        Assert.True(result.HostWindow);
        Assert.Null(result.ScreenId);
        Assert.Empty(result.Controls);
    }

    [Fact]
    public async Task CachedFrame_IsNotVisualized()
    {
        var (svc, frames, token) = Create(host: false);
        var result = await svc.ObserveAsync(token, CancellationToken.None);
        var frame = frames.Require(result.FrameId);
        Assert.False(frame.ImageReturnedToClient);
        Assert.NotNull(frame.Bgra);
    }

    private (ObserveService Svc, FrameCache Frames, string Token) Create(bool host)
    {
        var world = new FakeWorld();
        world.Processes[1] = new FakeProcess { Pid = 1, CreateTimeUtc = 1, ImagePath = @"c:\apps\app.exe" };
        world.Windows[1] = new FakeWindow { Hwnd = 1, Pid = 1, ClassName = "Notepad", WindowRect = new ScreenRect(0, 0, 200, 200), ExtendedFrameBounds = new ScreenRect(0, 0, 200, 200) };
        var tokens = new TargetTokenService();
        var token = tokens.Issue(1, 1, 1, "Notepad");
        var frames = new FrameCache(Limits.V1);
        var capture = new FakeCapture { Width = 80, Height = 80, Pixels = BgraFrames.Checker(80, 80) };
        var hostResolver = new StubHost { Result = host };
        var svc = new ObserveService(
            new DesktopOperationCoordinator(Limits.V1),
            tokens,
            frames,
            world,
            new FakeMonitors(),
            world,
            new FakeDesktops(),
            new FakeSession(),
            new FakeActivator { Foreground = 1 },
            capture,
            hostResolver,
            new MemoryCatalog(_root, Limits.V1),
            Limits.V1);
        return (svc, frames, token);
    }
}
